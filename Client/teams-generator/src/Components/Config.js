import { Card, DatePicker, Form, Input, Select } from "antd";
import MyCard from "./UI/MyCard";
import { QuestionCircleOutlined } from "@ant-design/icons";
import { useContext, useRef, useState } from "react";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import dayjs from 'dayjs';

const { Option } = Select;

function Config(props) {

    const configContext = useContext(ConfigurationContext);



    const layout = {
        labelCol: { span: 10 },
        wrapperCol: { span: 16 },
    };

    function shirtsColorsChangedHandler(e) {
        console.log("shirt", e)
        configContext.setSelectedShirtsColors(e)
    }

    function setNumberOfTeams(teamsCount) {
        configContext.config.config.numberOfTeams = teamsCount
        configContext.setConfig({ ...configContext.config, config: { ...configContext.config.config } })
    }

    function setEventDate(e,dateValue){
        console.log('e', typeof(e))
        configContext.config.config.eventDate =  e
        configContext.setConfig({ ...configContext.config, config: { ...configContext.config.config } })
    }

    return (

        <Card title='Configuration' style={{ width: '400px' }}>
            <QuestionCircleOutlined />  <label style={{ color: "gray", marginLeft: '4px' }}>Setup your settings of your teams</label>
            {configContext.config && <Form  {...layout} style={{ marginTop: '15px' }}>

                <Form.Item name="teamsCount" label="Number of Teams" >
                    <Input placeholder="Number of Teams" type="number" min={1} max={4} defaultValue={configContext.config.config.numberOfTeams} value={configContext.config.config.numbernumberOfTeamsOfTeams} onChange={(e) => { setNumberOfTeams(e.target.value) }} />
                </Form.Item>

                <Form.Item name="shirtsColors" label="Shirts Colors" >
                    <Select mode="multiple" placeholder="Select Shirts Colors" onChange={shirtsColorsChangedHandler}>
                        {configContext.config.config.shirtsColors.map(shirt => {
                            return <Option value={shirt} label={shirt} />
                        })}
                    </Select>
                </Form.Item>

                <Form.Item name="eventDate" label="Event Date">
                    <DatePicker onChange={setEventDate} format="YYYY-MM-DD HH:mm:ss" showTime={{ defaultValue: dayjs('00:00:00', 'HH:mm:ss') }}/>
                </Form.Item>
            </Form>}
        </Card>
    )

}


export default Config;