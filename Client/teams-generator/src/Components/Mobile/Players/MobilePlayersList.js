import { DeleteOutlined, QuestionCircleOutlined, UserAddOutlined, UserOutlined } from "@ant-design/icons";
import { Avatar, Button, Card, FloatButton, List, Popconfirm } from "antd";

function MobilePlayersList(props) {

    function playerListItemClicked(clickedPlayer) {
        props.onPlayerRemoved(clickedPlayer)
    }

    return (
        <Card style={{
            maxHeight: 700,
            overflow: 'auto',
            margin: '4px',
            padding: '0 16px',
            border: '1px solid rgba(140, 140, 140, 0.35)',
          }}>
            
            <List itemLayout="horizontal"  style={{  marginLeft: '-15px', marginRight: '-15px', marginTop: '-30px'}}
                  size="small" 
                  dataSource={props.players}
                  renderItem={(player, index) => (
                    <List.Item actions={[<Popconfirm icon={<QuestionCircleOutlined style={{ color: 'red' }} />} title='Remove Player' description='Are you sure you want to remove this player?' onConfirm={()=>{playerListItemClicked(player) }}><Button style={{marginTop: '15px', marginRight: '-18px'}} icon={<DeleteOutlined />} danger></Button></Popconfirm>]}>
                        <List.Item.Meta avatar={<Avatar style={{ marginTop: '20px' }}><UserOutlined  style={{ fontSize: '30px'}} /></Avatar>}
                                        title={<div style={{width: '100%', 'textOverflow': 'ellipsis', 'whiteSpace': 'nowrap', 'overflow': 'hidden', marginBottom: '-10px' }}>{player.Name}</div>}
                                        description={
                                            <label style={{fontSize: '12px'}}>{`${getDescription(props.playerProperties, player)}`}</label>
                                        }/>
                    </List.Item>
                )}>

            </List>
        </Card>
    )
}

function getDescription(playerProperties, player) {
    var max = {
        value: 0,
        name: 'Attack'
    }

    var min = {
        value: 10,
        name: 'Attack'
    }

    playerProperties.forEach(prop => {
        const playerPropValue = player[prop.name]
        if (playerPropValue > max.value) {
            max.name = prop.name
            max.value = playerPropValue
        }
        if (playerPropValue < min.value) {
            min.name = prop.name
            min.value = playerPropValue
        }
    });

    return `${max.name}: ${max.value}  ${min.name}: ${min.value}`
}

export default MobilePlayersList;