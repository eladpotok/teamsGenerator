import {  Col, Row} from "antd";
import MobilePlayersList from "./MobilePlayersList";

function MobilePlayersMenu(props) {

    return (
        <div style={{width: '100%'}}>
            {props.players && <div style={{height: '100%'}}>
                <Row style={{height: '100%'}}>
                    <Col flex='auto'>
                        <MobilePlayersList onPlayerArrived={props.onPlayerArrived}  onPlayerRemoved={props.onRemovePlayer} currentAlgo={props.currentAlgo} playerProperties={props.currentAlgo.playerProperties} players={props.players}/>
                    </Col>
                </Row>
            </div>}
        </div>
    )

}

export default MobilePlayersMenu;