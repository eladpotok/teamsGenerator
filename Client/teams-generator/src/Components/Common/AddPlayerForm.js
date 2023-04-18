import { Button, Form, Input } from "antd";
import { useContext } from "react";
import { v4 as uuidv4 } from 'uuid';
import { PlayersContext } from "../../Store/PlayersContext";
import AppButton from "./AppButton";

function AddPlayerForm(props) {

    const playersContext = useContext(PlayersContext)
    const [form] = Form.useForm();

    const layout = {
        labelCol: { span: 4 },
        wrapperCol: { span: 16 },
    };

    function addPlayerHandler(player) {
        playersContext.setPlayers([...playersContext.players, player])
    }

    const formFinishedHandler = (values) => {

        if(props.player) {
            const edittedPlayer = {...values, key: props.player.key}
            playersContext.editPlayer(edittedPlayer)
            form.setFieldsValue(edittedPlayer)
            props.onEditFinished()
        }
        else {
            values['key'] = uuidv4();
            values['isArrived'] = false
            addPlayerHandler(values)
            resetFromHandler();
        }
    };

    const resetFromHandler = () => {
        form.resetFields();
    };

    
    form.setFieldsValue(props.player)

    return (
        <Form {...layout} form={form} initialValues={props.player ? props.player : {}} onFinish={formFinishedHandler} style={{ marginTop: '15px' }} size="small">

            {props.playersProperties && props.playersProperties.filter(f => f.showInClient).map((property) =>

                <Form.Item valuePropName='value' name={property.name} label={property.name} rules={[{ required: true }]}>
                    <Input type={property.type} tep={0.1} max={10} min={1} />
                </Form.Item>

            )}

            <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                <AppButton  style={{ marginRight: 16 }}>
                    {props.player ? 'EDIT' : 'ADD'}
                </AppButton>

                <AppButton htmlType="button" onClick={resetFromHandler}>
                    RESET
                </AppButton>
            </div>

        </Form>
    )
}

function getValue(propertyName, player){
    if (player) return player[propertyName]
    return ''
    
}

export default AddPlayerForm;